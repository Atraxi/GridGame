import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import Layout from './Layout.tsx'
import LoginPage from './LoginPage.tsx'
import AccountPage from './AccountPage.tsx'
import MapEditorRoute from './CreateMapPage.tsx'
import { createBrowserRouter, RouterProvider } from 'react-router';
import GridGamePage from './GridGame.tsx';
import { loadJson, loadMapToEdit, loadNewMap } from './loaders.ts';
import { isGuest } from './auth.ts';
import RouteError from './RouteError.tsx';
import HelpPage from './HelpPage.tsx';
import { ForgotPasswordPage, ResetPasswordPage } from './PasswordResetPages.tsx';

const router = createBrowserRouter([
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
            path: "forgot-password",
            Component: ForgotPasswordPage,
          },
          {
            //The link emailed by UsersController.ForgotPassword
            path: "reset-password",
            Component: ResetPasswordPage,
          },
          {
            path: "help",
            Component: HelpPage,
          },
          {
            path: "account",
            Component: AccountPage,
            loader: async () => {
              //Past opponents are a named-account feature - asking as a guest would just be refused
              const [profile, maps, opponents] = await Promise.all([
                loadJson('/Users/Me'),
                loadJson('/Maps/GetMine'),
                isGuest() ? [] : loadJson('/Users/Opponents'),
              ])
              return { profile, maps, opponents }
            },
          },
          {
            path: "maps/create",
            Component: MapEditorRoute,
            loader: loadNewMap,
          },
          {
            path: "maps/:mapId/edit",
            Component: MapEditorRoute,
            loader: loadMapToEdit,
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
