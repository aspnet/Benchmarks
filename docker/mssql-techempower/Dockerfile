FROM mcr.microsoft.com/mssql/server:2019-latest

ENV ACCEPT_EULA=Y
ENV MSSQL_PID=Enterprise
# The 'sa' password has a minimum complexity requirement (8 characters, uppercase, lowercase, alphanumerical and/or non-alphanumerical)
ENV SA_PASSWORD=Benchmarkdbp@55

ADD entrypoint.sh .
ADD create.sql .
ADD import-data.sh .

USER root 

# Grant permissions for the import-data script to be executable
RUN chmod +x import-data.sh

USER mssql

CMD /bin/bash entrypoint.sh
