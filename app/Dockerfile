# app/Dockerfile — Angular 18 dev server
# Node 20 LTS Alpine base; npm ci for reproducible installs; ng serve with polling
# for reliable hot-reload on Windows Docker Desktop (inotify not supported in WSL2 mounts).

FROM node:20-alpine AS base

WORKDIR /app

# Copy package manifests first to leverage layer cache.
# npm ci is only re-run when package-lock.json changes.
COPY package*.json ./
RUN npm ci

# Copy remaining source files
COPY . .

EXPOSE 4200

# --host 0.0.0.0  → bind to all interfaces inside the container (required for port mapping)
# --poll 1000     → filesystem polling every 1 s (reliable on Windows Docker Desktop / WSL2)
CMD ["npx", "ng", "serve", "--host", "0.0.0.0", "--poll", "1000"]
