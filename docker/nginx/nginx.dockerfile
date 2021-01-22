FROM nginx:latest

ENV WORKER_PROCESSES auto
ENV PROXY_PASS http://tfb-database:5000

ADD https://github.com/aspnet/Benchmarks/raw/master/src/Benchmarks/testCert.crt /etc/ssl/certs/testCert.crt
ADD https://github.com/aspnet/Benchmarks/raw/master/src/Benchmarks/testCert.rsa /etc/ssl/private/testCert.rsa
ADD nginx.conf /etc/nginx/nginx.conf

ADD run.sh /
RUN chmod +x /run.sh

ENTRYPOINT ["/run.sh"]
