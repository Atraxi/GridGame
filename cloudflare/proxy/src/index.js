// Reverse proxy that puts a custom domain in front of the free-tier App Service.
//
// The F1 plan cannot bind a custom domain: App Service routes by the Host header and F1 only answers to its own
// *.azurewebsites.net name. Re-issuing each request against that name sends the Host it expects, while the browser
// only ever sees the custom domain - so the SPA, the API and the SignalR negotiate call stay on a single origin.
//
// ORIGIN_HOST is supplied at deploy time (see .github/workflows/deploy.yml), never committed.

export default {
  async fetch(request, env) {
    if (!env.ORIGIN_HOST) {
      return new Response('ORIGIN_HOST is not configured', { status: 500 })
    }

    const url = new URL(request.url)
    const publicOrigin = url.origin
    url.protocol = 'https:'
    url.hostname = env.ORIGIN_HOST
    url.port = ''

    // Copying the request keeps the method, headers (Authorization included) and body. The outbound Host follows
    // the URL, which is the whole point. Upgrade requests pass through as-is, so native WebSockets also work when
    // Azure SignalR Service is switched off.
    // Redirects are handed back to the browser rather than followed here, otherwise it would never learn the new URL
    const response = await fetch(new Request(url, request), { redirect: 'manual' })

    // Anything the app redirects to on its own hostname (or reports via Created's Location) would otherwise leak
    // the azurewebsites.net name and move the browser off the custom domain
    const location = response.headers.get('Location')
    const originPrefix = `https://${env.ORIGIN_HOST}`
    if (location?.startsWith(originPrefix)) {
      const rewritten = new Response(response.body, response)
      rewritten.headers.set('Location', publicOrigin + location.slice(originPrefix.length))
      return rewritten
    }

    return response
  },
}
