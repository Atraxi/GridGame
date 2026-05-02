import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { createBrowserRouter, RouterProvider } from 'react-router';
import GridGamePage from './GridGame.tsx';

let router = createBrowserRouter([
  {
    index: true,
    Component: App,
    loader: () =>
      fetch(`/Games/GetSummaries?page=1`,{
        headers: {
          "Content-Type": "application/json",
        }
      }),
  },
  {
    path: "/gridgame/:gameId",
    Component: GridGamePage,
    loader: (args) =>
      fetch(`/Games/GetGame?gameId=${args.params.gameId}`,{
        headers: {
          "Content-Type": "application/json",
        }
      }),
  },
]);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <RouterProvider router={router} />
  </StrictMode>,
)
