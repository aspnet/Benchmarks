FROM mcr.microsoft.com/dotnet/nightly/sdk:latest AS build-env
WORKDIR /src
COPY . ./
RUN dotnet publish --self-contained -r linux-x64 -c Release -o /publish

FROM node:18-bullseye-slim

# Install utilities
RUN apt-get update --fix-missing && apt-get -y upgrade && apt-get install -y git wget gnupg && apt-get clean

# Install latest chrome stable package
RUN wget -q -O - https://dl-ssl.google.com/linux/linux_signing_key.pub | apt-key add -
RUN sh -c 'echo "deb [arch=amd64] http://dl.google.com/linux/chrome/deb/ stable main" >> /etc/apt/sources.list.d/google.list'
RUN apt-get update \
    && apt-get install -y google-chrome-stable --no-install-recommends \
    && apt-get clean

# Install Lighthouse
RUN npm install -g lighthouse

# FIXME: Do not hard-code the server address
ENV SERVER_SCHEME=http
ENV SERVER_ADDRESS=10.0.0.102
ENV SERVER_PORT=5000

# Run lighthouse against the app URL
#RUN lighthouse $SERVER_SCHEME://$SERVER_ADDRESS:$SERVER_PORT \
    #--no-enable-error-reporting \
    #--chrome-flags="--no-sandbox --headless" \
    #--output=json \
    #--output-path=./report.json \
    #--only-audits="first-contentful-paint,interaction-to-next-paint,largest-contentful-paint,total-blocking-time"

COPY --from=build-env /publish /publish

#ENTRYPOINT ./publish/Lighthouse ./report.json
#ENTRYPOINT ["/publish/Lighthouse"]

ENTRYPOINT /publish/Lighthouse --url $SERVER_SCHEME://$SERVER_ADDRESS:$SERVER_PORT
