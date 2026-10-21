import { useState } from 'react'
import './App.css'
import GameList from './components/GameList'
import RulesCarousel from './components/RulesCarousel'
import StartGameDialog from './StartGameDialog'
import { isGuest } from './auth'

export default function App() {
  const [showStartGame, setShowStartGame] = useState(false)
  const guest = isGuest()

  return (
    <>
      <section className="home-intro">
        <h1>GridGame</h1>
        <p className="home-intro__tagline">
          A turn-based territory game. Spread across the grid, wall your rivals off, and hold the most ground when the
          board settles.
        </p>
      </section>

      <RulesCarousel />

      <section className="home-games">
        <div className="home-games__header">
          <h2>Games</h2>
          {guest
            ? <button disabled title="Create a named account to host games">Start new game</button>
            : <button className="home-games__start" onClick={() => setShowStartGame(true)}>Start new game</button>}
        </div>
        <GameList />
      </section>

      {showStartGame && <StartGameDialog onClose={() => setShowStartGame(false)} />}
    </>
  )
}
