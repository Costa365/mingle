FROM node:22.22.1-alpine AS build
WORKDIR /source
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-fund --no-audit
COPY web/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.28.0-alpine AS runtime
COPY infra/nginx-maps.conf /etc/nginx/mingle-maps.conf
COPY infra/nginx-server.conf /etc/nginx/mingle-server.conf
COPY infra/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /source/dist/ /usr/share/nginx/html/
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/healthz || exit 1
