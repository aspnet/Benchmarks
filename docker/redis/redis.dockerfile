FROM redis:6

# COPY redis.conf /etc/redis.conf

EXPOSE 6379

# CMD ["redis-server", "/etc/redis.conf"]

CMD ["redis-server"]
