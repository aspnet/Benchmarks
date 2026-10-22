# Postgres sidecar for HttpArena's async-db/fortunes profiles.
#
# HttpArena's own harness (scripts/lib/postgres.sh) runs a stock `postgres:18`
# container and bind-mounts data/pgdb-seed.sql straight from its working tree
# into /docker-entrypoint-initdb.d - there is no Dockerfile upstream to build
# from (Crank's dockerFile/dockerContextDirectory always need to resolve to a
# path that exists in a job's source). pgdb-seed.sql creates and seeds both
# tables these profiles need: `items` (async-db, 100K literal rows - ~7MB of
# text) and `fortune` (fortunes, 200 rows generated via generate_series).
#
# Rather than vendoring that ~7MB file as a git blob (it would be the largest
# tracked file in this whole repo), fetch it straight from HttpArena's own
# repository at build time, pinned to the exact same commit as the app
# source in scenarios/httparena.benchmarks.yml (keep these two in sync when
# bumping that pin) - the same "no vendored app source" approach already
# used for the aspnet-minimal-11/genhttp-ioxide apps themselves. `ADD` with a
# URL does not auto-extract (that only applies to local tar archives), so
# this is a plain file download.
FROM postgres:18

ADD https://raw.githubusercontent.com/MDA2AV/HttpArena/5b111b8889fee61076f1d7dd14fdb3dfd12c8492/data/pgdb-seed.sql /docker-entrypoint-initdb.d/seed.sql
# ADD from a URL downloads the file root-owned with restrictive permissions
# (unlike COPY from a local build context, which inherits the usual 644) -
# postgres's docker-entrypoint.sh runs initdb scripts as the postgres user,
# so without this the file exists but can't be read ("Permission denied").
RUN chmod 644 /docker-entrypoint-initdb.d/seed.sql

# HttpArena's harness starts postgres with `-c max_connections=256` (see
# scripts/lib/postgres.sh). The stock default (100) is too low for
# genhttp-ioxide's per-reactor connection pool (PgPool, one pool per CPU
# core, each sized DATABASE_MAX_CONN/reactors) on wide-core machines -
# confirmed on aspnet-gold-lin, where the default caused ~4% bad responses
# on async_db_genhttp_ioxide (pool connections refused once the stock
# 100-connection cap was exceeded).
CMD ["postgres", "-c", "max_connections=256"]
