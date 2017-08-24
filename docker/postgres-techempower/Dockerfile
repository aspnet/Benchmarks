FROM postgres:9

ADD create-postgres.sql max_connections.sh /docker-entrypoint-initdb.d/
RUN chmod 755 /docker-entrypoint-initdb.d/create-postgres.sql

ENV POSTGRES_USER=benchmarkdbuser \
    POSTGRES_PASSWORD=benchmarkdbpass \
    POSTGRES_DB=hello_world
