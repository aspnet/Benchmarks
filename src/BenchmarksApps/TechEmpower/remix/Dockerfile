FROM node:18-alpine AS base

# set for base and all layer that inherit from it
ENV NODE_ENV production

# Install all node_modules, including dev dependencies
FROM base as deps

WORKDIR /myapp

ADD package.json ./
RUN npm install --include=dev

# Setup production node_modules
FROM base as production-deps

WORKDIR /myapp

COPY --from=deps /myapp/node_modules /myapp/node_modules
ADD package.json ./
RUN npm prune --omit=dev

# Build the app
FROM base as build

WORKDIR /myapp

COPY --from=deps /myapp/node_modules /myapp/node_modules

ADD . .
RUN npm run build

# Finally, build the production image with minimal footprint
FROM base

WORKDIR /myapp

RUN addgroup --system --gid 1001 nodejs
RUN adduser --system --uid 1001 remix

COPY --from=production-deps --chown=remix:nodejs /myapp/node_modules /myapp/node_modules
COPY --from=build --chown=remix:nodejs /myapp/build /myapp/build
COPY --from=build --chown=remix:nodejs /myapp/public /myapp/public
ADD . .

USER remix

EXPOSE 3000

ENV PORT 3000
ENV DB_HOST postgres_te

CMD ["npm", "start"]