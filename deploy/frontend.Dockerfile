# Shared image for the three SPAs (web, seller, admin): build with Node, serve static with Nginx
FROM node:22-alpine AS build
ARG APP_DIR
WORKDIR /app
COPY ${APP_DIR}/package.json ${APP_DIR}/package-lock.json ./
RUN npm ci
COPY ${APP_DIR}/ ./
RUN npm run build

FROM nginx:1.27-alpine
COPY deploy/nginx/spa.conf /etc/nginx/conf.d/default.conf
COPY deploy/nginx/security-headers.inc /etc/nginx/snippets/security-headers.inc
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
