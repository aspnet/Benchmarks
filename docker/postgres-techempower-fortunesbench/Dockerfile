FROM postgres

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
    expect \
    && rm -rf /var/lib/apt/lists/*

ADD fortunesbench.sh fortunes.sql /
RUN chmod +x /fortunesbench.sh

ENV PGBENCH_HOST=localhost \
    PGBENCH_THREADS=1 \
    PGBENCH_CONNECTIONS=1 \
    PGBENCH_DURATION=10 \
    PGBENCH_QUERYMODE=prepared

ENTRYPOINT ["/fortunesbench.sh"]
