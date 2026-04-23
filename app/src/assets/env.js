// Runtime environment injection — substituted at Netlify build time.
// %%API_URL%% is replaced by the Netlify build command using sed before Angular bootstraps.
// For LOCAL DEVELOPMENT with PROXY: Use empty string to make requests to same origin (proxied to backend)
// For PRODUCTION: This will be substituted by Netlify build with the Railway API URL
(function (window) {
  window.__env = window.__env || {};
  // Empty string uses the same origin (Angular dev server will proxy to https://localhost:5001)
  window.__env.apiUrl = "";
})(window);
