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

COPY --from=build-env /publish /publish

ENV TARGET_URL=http://localhost:5000

ENTRYPOINT /publish/Lighthouse --url $TARGET_URL
