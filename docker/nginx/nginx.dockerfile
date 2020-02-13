FROM nginx:latest

ENV WORKER_PROCESSES auto
ENV PROXY_PASS http://tfb-database:5000

COPY nginx.conf /etc/nginx/nginx.conf

CMD sed -i "s|WORKER_PROCESSES|$WORKER_PROCESSES|g" /etc/nginx/nginx.conf
CMD sed -i "s|PROXY_PASS|$PROXY_PASS|g" /etc/nginx/nginx.conf
