import './App.css'
import GameList from './components/GameList'

export default function App() {
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

      <GameList/>
    </>
  )
}