FROM node:22-alpine
WORKDIR /app
COPY server/package.json ./package.json
COPY server/server.js ./server.js
COPY site ./site
RUN mkdir -p /app/data && chown -R node:node /app
USER node
EXPOSE 3080
CMD ["node", "server.js"]
