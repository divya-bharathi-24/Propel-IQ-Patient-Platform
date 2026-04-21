// Runtime environment values are injected via assets/env.js at Netlify build time.
// window.__env.apiUrl is substituted from the API_URL Netlify build environment variable.
// The empty-string fallback is deliberate: API calls will fail visibly (not silently)
// if env.js is absent or the %%API_URL%% token was not substituted — satisfying AC-3.
export const environment = {
  production: false,
  apiUrl:
    (window as unknown as { __env?: { apiUrl?: string } }).__env?.apiUrl ?? '',
};
