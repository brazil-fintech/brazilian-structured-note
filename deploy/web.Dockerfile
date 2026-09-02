# The React app behind nginx, built from the repository root:
#     docker build -f deploy/web.Dockerfile -t coe-web .
#
# nginx serves the built assets and proxies /api to the API container, so the browser sees a
# single origin and no preflight sits in front of every validation call. Point it elsewhere
# with COE_API_PROXY_PASS, or drop the proxy entirely with COE_API_BASE_URL (see
# deploy/web-entrypoint.sh).

FROM node:22-alpine AS build
WORKDIR /web

COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ ./
# The app sits at the root of its origin here; only the GitHub Pages build needs a prefix.
ARG VITE_BASE_PATH=/
ENV VITE_BASE_PATH=$VITE_BASE_PATH
RUN npm run build

FROM nginx:1.27-alpine AS runtime
COPY --from=build /web/dist /usr/share/nginx/html
COPY deploy/web-nginx.conf.template /etc/nginx/templates/default.conf.template
COPY deploy/web-entrypoint.sh /docker-entrypoint.d/40-coe-config.sh
RUN chmod +x /docker-entrypoint.d/40-coe-config.sh

ENV COE_API_PROXY_PASS=http://api:8080 \
    COE_API_BASE_URL=/api \
    NGINX_PORT=8080

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=3 \
    CMD wget --quiet --spider http://localhost:8080/ || exit 1
