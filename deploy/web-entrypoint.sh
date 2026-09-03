#!/bin/sh
# Writes the runtime configuration the app reads on load, so one built image can point at any
# API: the same tag runs against the container next door or against a deployed instance.
#
#   COE_API_BASE_URL=/api                     → nginx proxies to COE_API_PROXY_PASS (default)
#   COE_API_BASE_URL=https://coe.example/api  → the browser calls that host directly, which
#                                               needs the origin listed in the API's Cors:Origins
set -eu

config="${COE_WEB_ROOT:-/usr/share/nginx/html}/config.js"
base_url="${COE_API_BASE_URL:-/api}"

cat > "$config" <<JS
// Written at container start from COE_API_BASE_URL — edit the variable, not this file.
window.__COE_CONFIG__ = { apiBaseUrl: '${base_url}' };
JS

echo "coe-web: API base URL is ${base_url}"
