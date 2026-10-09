# syntax=docker/dockerfile:1
#
# The customer website: built with Node, served by an unprivileged nginx that also forwards /api/ to the API.
# Build context: this directory (frontend/).
#
#   docker build -t vehicle-rental-web frontend

# ---- build: install from the lockfile, typecheck and bundle ----
FROM node:25-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

# ---- serve: static files only, no Node at runtime ----
FROM nginxinc/nginx-unprivileged:stable-alpine AS web
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY security-headers.inc /etc/nginx/security-headers.inc
COPY --from=build /app/dist /usr/share/nginx/html
# Runs as the image's unprivileged user and listens on 8080.
EXPOSE 8080
