FROM mongo:3.4.10

ADD init.js /docker-entrypoint-initdb.d/
RUN chmod 755 /docker-entrypoint-initdb.d/init.js
