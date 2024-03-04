FROM node:18-bullseye-slim

# Copy the context folder to this image
WORKDIR /src
COPY . ./

# Install utilities
RUN apt-get update --fix-missing && apt-get -y upgrade && apt-get install -y git wget gnupg && apt-get clean

# Install latest chrome stable package
RUN wget -q -O - https://dl-ssl.google.com/linux/linux_signing_key.pub | apt-key add -
RUN sh -c 'echo "deb [arch=amd64] http://dl.google.com/linux/chrome/deb/ stable main" >> /etc/apt/sources.list.d/google.list'
RUN apt-get update \
    && apt-get install -y google-chrome-stable --no-install-recommends \
    && apt-get clean

# Install node packages
RUN npm install

ENV SCENARIO_SCRIPT=
ENV SCENARIO_SCRIPT_ARGS=

ENTRYPOINT node ./src/$SCENARIO_SCRIPT $SCENARIO_SCRIPT_ARGS --job-url $CRANK_JOB_LOCAL_URL
