FROM node:22-alpine AS build
WORKDIR /web
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:1.28-alpine
COPY nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=build /web/dist /usr/share/nginx/html
# Where nginx forwards /api. The official image substitutes this into the template at start-up.
ENV API_UPSTREAM=http://api:8080
EXPOSE 80
