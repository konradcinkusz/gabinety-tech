# syntax=docker/dockerfile:1
# Gabinety web (Next.js standalone). Build context: ./web. No secrets are needed or accepted at build time;
# every runtime value (Backend__*, Site__BaseUrl, Billing__FirstConsultationFeeBps, ...) arrives as environment at run time.

FROM node:22-alpine AS deps
RUN corepack enable
WORKDIR /app
COPY package.json pnpm-lock.yaml ./
RUN --mount=type=cache,target=/root/.local/share/pnpm/store pnpm install --frozen-lockfile

FROM node:22-alpine AS build
RUN corepack enable
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN pnpm build

FROM node:22-alpine AS runtime
LABEL org.opencontainers.image.source="https://github.com/konradcinkusz/gabinety-tech" \
      org.opencontainers.image.title="gabinety-web"
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1 PORT=8080 HOSTNAME=0.0.0.0 TZ=Europe/Warsaw
RUN apk add --no-cache tzdata
WORKDIR /app
COPY --from=build --chown=node:node /app/.next/standalone ./
COPY --from=build --chown=node:node /app/.next/static ./.next/static
COPY --from=build --chown=node:node /app/content ./content
USER node
EXPOSE 8080
CMD ["node", "server.js"]
