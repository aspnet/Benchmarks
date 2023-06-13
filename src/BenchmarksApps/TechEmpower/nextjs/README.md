# Fortunes implementation using Next.js

This is an implementation of the [TechEmpower Fortunes benchmark](https://github.com/TechEmpower/FrameworkBenchmarks/wiki/Project-Information-Framework-Tests-Overview#fortunes) using Next.js

Run the app by executing `npm run dev` in the app root.

*NOTE: Currently using Next.js version 13.4.0 rather than latest due to [this issue](https://github.com/vercel/next.js/issues/48173#issuecomment-1584585902)*

The [Dockerfile](./Dockerfile) will build a standalone image (based on node-alpine) for running the app on port 3000.
