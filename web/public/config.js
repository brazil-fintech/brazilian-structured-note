// Runtime configuration, read by the app on load. The container image rewrites this file at
// start from COE_API_BASE_URL and the GitHub Pages workflow writes it from the repository
// variable COE_API_BASE_URL, so the same built bundle can be pointed at any API.
window.__COE_CONFIG__ = { apiBaseUrl: '/api' };
