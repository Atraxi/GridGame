import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

//Every route the API owns. Listed once so the dev proxy and any future rewrite rules stay in step -
//in production these are same-origin, because the build below lands in the API's wwwroot
const apiRoutes = ['/Games', '/Users', '/Maps', '/GridGame']

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    //Published straight into the API project so `dotnet publish` picks the bundle up as static content
    //and one App Service instance serves the client, the API and the SignalR hub from a single origin
    outDir: '../GridGame.Server/GridGameAPI/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: Object.fromEntries(apiRoutes.map(route => [route, {
      target: 'http://localhost:5095',
      changeOrigin: true,
      secure: false,
      //SignalR's preferred transport is a WebSocket upgrade, which the proxy only forwards when asked
      ws: true,
    }])),
  },
})
