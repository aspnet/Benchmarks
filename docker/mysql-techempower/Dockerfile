FROM mysql:5.7.19

ADD create.sql /docker-entrypoint-initdb.d/
ADD my.cnf /etc/mysql/conf.d/
RUN chmod 755 /docker-entrypoint-initdb.d/create.sql

ENV MYSQL_USER=benchmarkdbuser \
    MYSQL_PASSWORD=benchmarkdbpass \
    MYSQL_ROOT_PASSWORD=benchmarkdbpass \
    MYSQL_DATABASE=hello_world
