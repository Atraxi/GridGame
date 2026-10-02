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
import { loadJson } from './loaders.ts';
import RouteError from './RouteError.tsx';

let router = createBrowserRouter([
  {
    Component: Layout,
    children: [
      {
        //Pathless, so an error in any page renders inside the Layout (nav included) instead of replacing the whole app
        ErrorBoundary: RouteError,
        children: [
          {
            index: true,
            Component: App,
            loader: () => loadJson(`/Games/GetSummaries?page=1`),
          },
          {
            path: "gridgame/:gameId",
            Component: GridGamePage,
            loader: (args) => loadJson(`/Games/GetGame?gameId=${args.params.gameId}`),
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
                loadJson('/Users/Me'),
                loadJson('/Maps/GetMine'),
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
    ],
  },
]);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <RouterProvider router={router} />
  </StrictMode>,
)
