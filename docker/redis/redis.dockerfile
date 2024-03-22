FROM mcr.microsoft.com/cbl-mariner/base/redis:6

COPY redis.conf /etc/redis.conf

EXPOSE 6379

CMD ["redis-server", "/etc/redis.conf"]
