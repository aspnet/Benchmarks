FROM mongo:3.4.10

ADD create.js /docker-entrypoint-initdb.d/
RUN chmod 755 /docker-entrypoint-initdb.d/create.js
