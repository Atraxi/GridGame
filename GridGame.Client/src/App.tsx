import { useState } from 'react'
import './App.css'
import GameList from './components/GameList'
import StartGameDialog from './StartGameDialog'
import { isGuest } from './auth'

export default function App() {
  const [showStartGame, setShowStartGame] = useState(false)
  const guest = isGuest()

  return (
    <>
      <section id="center">
        <div className="hero">
        </div>
        <div>
          <h1>Get started</h1>
          <p>
            Edit <code>src/App.tsx</code> and save to test <code>HMR</code>
          </p>
        </div>

      </section>

      <section id="spacer"></section>

      {guest
        ? <button disabled title="Create a named account to host games">Start New Game</button>
        : <button onClick={() => setShowStartGame(true)}>Start New Game</button>}
      <GameList/>

      {showStartGame && <StartGameDialog onClose={() => setShowStartGame(false)} />}
    </>
  )
}
