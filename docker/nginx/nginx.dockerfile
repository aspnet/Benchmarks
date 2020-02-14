FROM nginx:latest

ENV WORKER_PROCESSES auto
ENV PROXY_PASS http://tfb-database:5000

ADD nginx.conf /etc/nginx/nginx.conf

ADD run.sh /
RUN chmod +x /run.sh

ENTRYPOINT ["/run.sh"]
