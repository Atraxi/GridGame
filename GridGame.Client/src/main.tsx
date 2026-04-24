import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { createBrowserRouter, RouterProvider } from 'react-router';
import GridGame from './GridGame.tsx';

let router = createBrowserRouter([
  {
    index: true,
    Component: App,
    loader: () =>
      fetch(`/Games/GetSummaries?page=1`),
  },
  {
    path: "/gridgame/:gameId",
    Component: GridGame,
    loader: (args) =>
      fetch(`/Games/GetGame?gameId=${args.params.gameId}`),
  },
]);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <RouterProvider router={router} />
  </StrictMode>,
)
