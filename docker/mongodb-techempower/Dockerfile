FROM mongo:3.4.10

COPY init.js ./init.js

RUN mongo benchmarks --host mongodb:27017 ./init.js
