FROM jenkins/jenkins:lts

USER root

ENV DOTNET_CLI_TELEMETRY_OPTOUT 1
ENV DOTNET_SKIP_FIRST_TIME_EXPERIENCE 1

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libc6 \
        libcurl3 \
        libgcc1 \
        libgssapi-krb5-2 \
        libicu57 \
        liblttng-ust0 \
        libssl1.0.2 \
        libstdc++6 \
        libunwind8 \
        libuuid1 \
        zlib1g \
        sshpass \
    && rm -rf /var/lib/apt/lists/*

ENV PATH="/root/.dotnet:${PATH}"

RUN curl https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain/dotnet-install.sh -o dotnet-install.sh \
    && chmod +x dotnet-install.sh \
    && ./dotnet-install.sh --channel Current

COPY executors.groovy /usr/share/jenkins/ref/init.groovy.d/executors.groovy

CMD sudo echo $'jenkins    ALL = NOPASSWD: /path/to/script\n' >> /etc/sudoers
