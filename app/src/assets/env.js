// Runtime environment injection — substituted at Netlify build time.
// %%API_URL%% is replaced by the Netlify build command using sed before Angular bootstraps.
// DO NOT hardcode values here. This file is committed with placeholder tokens only.
// The empty string fallback is intentional: API calls will fail visibly (not silently)
// if env.js is not loaded or the substitution was not applied (AC-3).
(function (window) {
  window.__env = window.__env || {};
  window.__env.apiUrl = "%%API_URL%%";
})(window);
