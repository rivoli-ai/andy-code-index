FROM node:20-alpine AS build
WORKDIR /app
ENV NODE_TLS_REJECT_UNAUTHORIZED=0 NPM_CONFIG_STRICT_SSL=false
COPY package*.json ./
RUN npm ci
COPY . .
RUN npx ng build --configuration production

FROM nginx:alpine
COPY --from=build /app/dist/client/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
