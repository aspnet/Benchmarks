import { writeFileSync } from 'fs';
import puppeteer from 'puppeteer';
import { startFlow } from 'lighthouse';
import { program } from 'commander';
import { logReadyStateText } from './utilities/crank-helpers.js';
import { LighthouseCrankReporter } from './utilities/lighthouse-crank-reporter.js';

program.requiredOption('--target-base-url <url>', 'The base URL of the application to test against');
program.option('--enforce-wasm-caching', 'Ensures that WASM resources are downloaded before the second page load');
program.option('--job-url <url>', 'The URL of the crank job to receive the statistics report');
program.option('--result-file <file>', 'The file to which the JSON test results should be written');
program.parse();

const {
  targetBaseUrl,
  enforceWasmCaching,
  jobUrl,
  resultFile,
} = program.opts();

logReadyStateText(); // Required by the Crank job

const browser = await puppeteer.launch({ args: ['--no-sandbox'] });
const pageUrl = `${targetBaseUrl}/counter`;

// Warm up the target server so that any server-side caches are fully initialized
await performWarmUpPageLoad(browser, pageUrl);

const page = await browser.newPage();

// Forward logs from the browser to standard out for debugging purposes
page.on('console', (msg) => console.log(`[Browser]: ${msg.text()}`));

const flow = await startFlow(page);

// Step 0: Load with an empty cache
await runStep('First page load', async (name) => {
  await flow.navigate(pageUrl, { name });
});

// Sanity check: If we're expecting WebAssembly resources to download on the first
// page visit (e.g., WebAssembly/Auto render modes), then wasm resources should
// be cached by this point. Let's fail if we see wasm resources get downloaded
// in the future, because that might indicate that the first navigation step
// did not capture everything it should have.
if (enforceWasmCaching) {
  throwOnFutureUncachedWasmDownloads(page);
}

// Sanity check: Ensure that the counter is interactive before continuing
await incrementCounter(/* enableRetries */ true);

// Step 1: Reload with the cache populated
await runStep('Second page load', async (name) => {
  await flow.navigate(pageUrl, { name });
})

// Wait for the counter to become interactive so that we
// can later measure the impact of clicking the counter a single time
// without having to retry counter clicks
await incrementCounter(/* enableRetries */ true);

// Step 2: Collect interaction metrics by clicking the counter
await runStep('Interaction', async (name) => {
  await flow.startTimespan({ name });
  await incrementCounter(/* enableRetries */ false);
  await flow.endTimespan();
});

await browser.close();

// Process results
const flowResult = await flow.createFlowResult();

if (resultFile) {
  console.log(`Writing the results to '${resultFile}'...`);
  writeFileSync(resultFile, JSON.stringify(flowResult, null, 2));
}

if (jobUrl) {
  await reportStatistics(flowResult, jobUrl);
}

console.log('Scenario complete.');

/**
 * Warms up the target server by loading the page in a separate browser context
 * @param {import("puppeteer").Browser} browser
 * @param {string} pageUrl
 */
async function performWarmUpPageLoad(browser, pageUrl) {
  console.log('Performing warm-up page load...');
  const context = await browser.createBrowserContext();
  const page = await context.newPage();
  await page.goto(pageUrl);

  console.log('Waiting for the network to be idle...');
  await page.waitForNetworkIdle();

  console.log('Warm-up complete.');
}

/**
 * Runs the provided user flow step, logging message before and after
 * the step.
 * @param {string} name The name of the step
 * @param {(name: string) => Promise<void>} callback The function performing the step
 */
async function runStep(name, callback) {
  console.log(`==== Starting step '${name}'... ====`);
  await callback(name);
  console.log(`====  Completed step '${name}'  ====`);
}

/**
 * Throws an error if any WASM resources get downloaded beyond this point
 * @param {import("puppeteer").Page} page 
 */
function throwOnFutureUncachedWasmDownloads(page) {
  console.log('Enabling WASM resource caching enforcement');

  page.on('requestfinished', request => {
    const url = request.url();
    if (url.endsWith('.wasm') && request.response().status() !== 304) {
      throw new Error(`Unexpected uncached wasm resource '${url}'`);
    }
  });
}

/**
 * Clicks the counter button and waits for its label to update.
 * If retries are enabled and the label doesn't update within the expected period,
 * the button gets clicked again.
 * @param {boolean} enableRetries Whether the button should be clicked again if the first
 * click didn't cause an increment.
 */
async function incrementCounter(enableRetries) {
  const maxAttempts = 3;
  const pollInterval = 1000;

  for (let i = 0; i < maxAttempts; i++) {
    const didIncrement = await page.evaluate(incrementCounterCore);

    if (didIncrement) {
      return;
    }

    if (!enableRetries) {
      throw new Error('Could not increment the counter on the first attempt');
    }

    await new Promise(resolve => setTimeout(resolve, pollInterval));
  }

  throw new Error(`Could not increment the counter within ${maxAttempts} attempts`);

  async function incrementCounterCore() {
    const counterButtonSelector = 'body > div.page > main > article > button';
    const counterLabelSelector = 'body > div.page > main > article > p';

    // Ensure that the counter button is visible before proceeding
    const counterButton = document.querySelector(counterButtonSelector);
    if (!counterButton) {
      console.log('The counter button is not visible...');
      return false;
    }

    // Record the initial counter text so we can detect when the counter updates
    const initialCounterLabelText = document.querySelector(counterLabelSelector).textContent;

    console.log('Clicking the counter button...');
    counterButton.click();

    const maxCountUpdateChecks = 10;
    const countUpdateCheckPollInterval = 500;
    for (var i = 0; i < maxCountUpdateChecks; i++) {
      await new Promise(resolve => setTimeout(resolve, countUpdateCheckPollInterval));

      const newCounterLabelText = document.querySelector(counterLabelSelector).textContent;
      if (newCounterLabelText !== initialCounterLabelText) {
        console.log(`The counter label changed from '${initialCounterLabelText}' to '${newCounterLabelText}'`);
        return true;
      }

      console.log(`Waiting for the current count to update (${i + 1})...`);
    }

    console.log('The current count did not update in the expected period');
    return false;
  }
}

/**
 * Reports statistics to the specified Crank job URL
 * @param {import("lighthouse").FlowResult} flowResult
 * @param {string} jobUrl
 */
async function reportStatistics(flowResult, jobUrl) {
  console.log('Collecting Crank statistics...');

  const reporter = new LighthouseCrankReporter(flowResult, /* metadataPrefix */ 'lighthouse-blazor');
  addNavigationMeasurements(reporter, /* stepId */ 0);
  addNavigationMeasurements(reporter, /* stepId */ 1);
  addInteractionMeasurements(reporter, /* stepId */ 2);

  console.log('Statistics:');
  console.log(reporter.getStatisticsJson());

  console.log(`Reporting results to '${jobUrl}'...`);
  await reporter.submitStatistics(jobUrl);
}

/**
 * Adds navigation measurements for the given Lighthouse user flow step
 * @param {LighthouseCrankReporter} reporter
 * @param {number} stepId
 */
function addNavigationMeasurements(reporter, stepId) {
  reporter.measureAudit(stepId, 'first-contentful-paint');
  reporter.measureAudit(stepId, 'largest-contentful-paint');
  reporter.measureAudit(stepId, 'total-blocking-time');
  reporter.measureAudit(stepId, 'interactive');
}

/**
 * Adds interaction measurements for the given Lighthouse user flow step
 * @param {LighthouseCrankReporter} reporter 
 * @param {number} stepId 
 */
function addInteractionMeasurements(reporter, stepId) {
  reporter.measureAudit(stepId, 'total-blocking-time');
}
