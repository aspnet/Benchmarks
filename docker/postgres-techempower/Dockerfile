FROM postgres

ENV POSTGRES_USER benchmarkdbuser
ENV POSTGRES_PASSWORD benchmarkdbpass
ENV POSTGRES_DB hello_world

ADD max_connections.sh /docker-entrypoint-initdb.d/

ADD https://raw.githubusercontent.com/TechEmpower/FrameworkBenchmarks/master/toolset/setup/linux/databases/postgresql/create-postgres.sql /docker-entrypoint-initdb.d/
RUN chmod 755 /docker-entrypoint-initdb.d/create-postgres.sql
