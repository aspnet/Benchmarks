FROM redis:6

COPY redis.conf /etc/redis.conf

CMD ["redis-server", "/etc/redis.conf"]
