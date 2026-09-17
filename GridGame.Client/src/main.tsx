import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import Layout from './Layout.tsx'
import LoginPage from './LoginPage.tsx'
import AccountPage from './AccountPage.tsx'
import CreateMapPage from './CreateMapPage.tsx'
import { createBrowserRouter, RouterProvider } from 'react-router';
import GridGamePage from './GridGame.tsx';
import { authorizedFetch } from './auth.ts';

let router = createBrowserRouter([
  {
    Component: Layout,
    children: [
      {
        index: true,
        Component: App,
        loader: () => authorizedFetch(`/Games/GetSummaries?page=1`),
      },
      {
        path: "gridgame/:gameId",
        Component: GridGamePage,
        loader: (args) => authorizedFetch(`/Games/GetGame?gameId=${args.params.gameId}`),
      },
      {
        path: "login",
        Component: LoginPage,
      },
      {
        path: "account",
        Component: AccountPage,
        loader: async () => {
          const [profile, maps] = await Promise.all([
            authorizedFetch('/Users/Me').then(response => response.json()),
            authorizedFetch('/Maps/GetMine').then(response => response.json()),
          ])
          return { profile, maps }
        },
      },
      {
        path: "maps/create",
        Component: CreateMapPage,
      },
    ],
  },
]);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <RouterProvider router={router} />
  </StrictMode>,
)
