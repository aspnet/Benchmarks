FROM microsoft/mssql-server-linux:2017-GA

# Create app directory
RUN mkdir -p /usr/src/app
WORKDIR /usr/src/app

# Bundle app source
COPY . /usr/src/app

# Grant permissions for the import-data script to be executable
RUN chmod +x /usr/src/app/import-data.sh
RUN chmod +x /usr/src/app/entrypoint.sh

RUN /usr/src/app/entrypoint
