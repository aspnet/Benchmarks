import cluster from 'node:cluster';
import process from 'node:process';
import os from 'node:os';
import fs from 'node:fs';
import { createServer } from 'http';
import { parse } from 'url';

const dev = process.env.NODE_ENV !== 'production'

// Make sure commands gracefully respect termination signals (e.g. from Docker)
// Allow the graceful termination to be manually configurable
if (!process.env.NEXT_MANUAL_SIG_HANDLE) {
  process.on('SIGTERM', () => process.exit(0))
  process.on('SIGINT', () => process.exit(0))
}

if (!dev) {
  const servers = ['./.next/standalone/server.js', './server.js'];
  let server = '';

  for (var i = 0; i < servers.length; i++) {
    if (fs.existsSync(servers[i])) {
      server = fs.realpathSync(servers[i]);
      break;
    }
  }

  if (server === '') {
    console.error('Could not find server.js');
      process.exit(-1);
  }

  //console.log(`server.js found at: ${server}`);

  if (cluster.isPrimary) {
    console.log(`Primary is running on process ${process.pid}`);
    
    cluster.setupPrimary({ silent: true });
    const numWorkers = process.env.WORKER_COUNT ? parseInt(process.env.WORKER_COUNT) : os.cpus().length;

    // Fork workers
    let listening = 0;
    for (let i = 0; i < numWorkers; i++) {
      const w = cluster.fork();
      w.process.stdout?.on('data', (chunk) => {
        const msg = chunk.toString().trim();
        if (msg.startsWith('Listening on port')) {
          listening++;
          console.log(`Worker ${listening} of ${numWorkers} on process ${w.process.pid}: ${msg}`);
          if (listening == numWorkers) {
            console.log('All workers listening.');
          }
        } else {
          console.log(`Worker ${w.id}: ${msg}`)
        }
      });
    }

    cluster.on('exit', (worker, code, signal) => {
      console.log(`worker ${worker.process.pid} stopped`, { code, signal });
    });
  } else {
    require(server);
  }
} else {
  console.log('Development mode');

  const next = require('next');
  const app = next({ dev });
  const handle = app.getRequestHandler();
  const port = parseInt(process.env.PORT || '3000', 10);

  app.prepare().then(() => {
    createServer((req, res) => {
      const parsedUrl = parse(req.url!, true)
      handle(req, res, parsedUrl)
    }).listen(port);
  
    console.log(
      `> Server listening at http://localhost:${port} as ${
        dev ? 'development' : process.env.NODE_ENV
      }`
    );
  });
}
