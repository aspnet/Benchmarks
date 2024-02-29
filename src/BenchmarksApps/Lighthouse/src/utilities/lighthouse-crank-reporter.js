/** @typedef {import("lighthouse").FlowResult} FlowResult */

/** @typedef {'First'|'Last'|'Avg'|'Sum'|'Median'|'Max'|'Min'|'Count'|'All'|'Delta'} Operation */

/**
 * @typedef Metadata
 * @prop {string} name
 * @prop {Operation} aggregate
 * @prop {Operation} reduce
 * @prop {string} format
 * @prop {string} longDescription
 * @prop {string} shortDescription
 */

/**
 * @typedef Measurement
 * @prop {string} name
 * @prop {string} timeStamp
 * @prop {any} value
 */

/**
 * A utility to collect Lighthouse measurements and report them to Crank
 */
export class LighthouseCrankReporter {
  /**
   * Constructs a new {@link LighthouseCrankReporter} instance
   * @param {FlowResult} flowResult The flow result collected from a Lighthouse user flow
   * @param {string} metadataPrefix The prefix to prepend to each metadata entry
   */
  constructor(flowResult, metadataPrefix) {
    this.flowResult = flowResult;
    this.metadataPrefix = metadataPrefix;
    this.statistics = {
      /** @type {Metadata[]} */
      metadata: [],
      /** @type {Measurement[]} */
      measurements: [],
    };

    /** @type {Set<string>} */
    this.metadataIds = new Set();
  }

  /**
   * Submits the report to the Crank job at the specified URL
   * @param {string} jobUrl The Crank job URL
   * @returns {Promise<void>}
   */
  async submitStatistics(jobUrl) {
    const response = await fetch(`${jobUrl}/statistics`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(this.statistics),
    });

    if (!response.ok) {
      console.error(`Received reponse status code ${response.status} when submitting statistics: ${response.statusText}`);
    }
  }

  /**
   * Returns the JSON-stringified statistics
   */
  getStatisticsJson() {
    return JSON.stringify(this.statistics, null, 2);
  }

  /**
   * Measures a Lighthouse audit and includes it in the statistics report
   * @param {number} stepId 
   * @param {string} auditId 
   * @param {Operation?} aggregate
   * @param {Operation?} reduce
   */
  measureAudit(stepId, auditId, aggregate, reduce) {
    const step = this.flowResult.steps[stepId];
    const audit = step.lhr.audits[auditId];
    if (!audit) {
      throw new Error(`No audit with the ID '${auditId}' was reported in step '${stepId}'`);
    }

    const metadataId = this.getMetadataId(stepId, auditId);

    if (!this.metadataIds.has(metadataId)) {
      this.metadataIds.add(metadataId);
      this.statistics.metadata.push({
        name: metadataId,
        aggregate: aggregate || 'First',
        reduce: reduce || 'First',
        format: 'n0',
        shortDescription: `${audit.title} (${step.name})`,
        longDescription: audit.description,
      });
    }

    this.statistics.measurements.push({
      name: metadataId,
      value: audit.numericValue,
      timeStamp: step.lhr.fetchTime,
    });
  }

  /**
   * @private
   * @param {number} stepId
   * @param {string} auditId
   */
  getMetadataId(stepId, auditId) {
    return `${this.metadataPrefix}/step-${stepId}/${auditId}`;
  }
}
