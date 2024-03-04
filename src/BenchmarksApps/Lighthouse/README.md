# Lighthouse benchmarks

Benchmarks that utilize [Lighthouse](https://developer.chrome.com/docs/lighthouse) to perform various well-known web audits.

## How these benchmarks work

The `lighthouse` job sets up a Docker image configured to run a [Lighthouse user flow](https://web.dev/articles/lighthouse-user-flows) script defined in JavaScript. Various scenarios conducting user flows and reporting statistics can be found in the `./src/` subdirectory.

The `script` job variable is a path to the scenario JavaScript file to run, relative to the `./src/` subdirectory, and the `scriptArgs` variable defines the command-line arguments to be passed to the script. In addition to the specified `scriptArgs`, a `--job-url` command-line argument will be passed to the script when running with Crank. This is the URL to which the script should report its statistics.

It should be noted that the Lighthouse scenario script doesn't define the app to test - it defines the steps that collect Lighthouse scores for an app. The actual app should be running as part of the separately-defined "application" job, whereas the Lighthouse script should run as part of the "load" job. As an example, the Blazor scenarios work by using the `runTemplate` job to build, publish, and run a Blazor Web App from the project template, then passing the app's host URL to the `./src/blazor-scenario.js` script.

The `/scenarios/blazor.benchmarks.yml` configuration file and `./src/blazor-scenario.js` scenario script might be helpful as a reference when creating additional Lighthouse scenarios.

## Running a Lighthouse benchmark

When developing a Lighthouse test, it's often easiest to run the target app locally and launch the scenario script manually. For example, an easy way to run the Blazor scenario locally is to:
1. Create a Blazor Web App with `dotnet new blazor` (with whichever interactivity flag you want).
2. Run the created app and take note of the app's host URL.
3. Run the scenario script using `node` on the CLI. For example, from the `./src/` subdirectory:
   ```text
   node ./blazor-scenario.js --target-base-url http://localhost:5000 --result-file ./results.json
   ```

This technique is useful when quickly iterating on the Lighthouse user flow, but at some point it will be necessary to run it using its Crank integration. For that, use one of the following approaches:

### Running Crank using code in `main`

Following is an example of a Crank command that runs one of the Blazor Web App Lighthouse benchmarks:
```text
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/blazor.benchmarks.yml --scenario blazorWebInteractiveAuto --profile aspnet-perf-lin --application.framework net9.0
```

You can use different `--config` and `--scenario` options to point to other Lighthouse scenarios.

### Running Crank using code in another branch

Following is a Crank command that points to a scenario defined in a specific branch:
```text
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/<BRANCH>/<CONFIG_FILE> --scenario <SCENARIO_NAME> --profile aspnet-perf-lin --application.framework net9.0 --application.source.branchOrCommit <BRANCH> --load.source.branchOrCommit <BRANCH>
```

**Note:** The specified config file may contain imports that point to `main`. If your branch contains changes to any imported config files, those import paths should be updated.

### Running Crank using local code

Following is a Crank command that uses a local copy of this repository:
```text
crank --config <LOCAL_SCENARIO_CONFIG_FILE> --scenario <SCENARIO_NAME> --profile aspnet-perf-lin --application.framework net9.0 --application.source.localFolder <LOCAL_REPO_ROOT> --load.source.localFolder <LOCAL_REPO_ROOT>
```

**Note:**
The specified local config file may contain imports that point to non-local sources. If your local copy contains changes to any imported config files, those import paths should be updated.
