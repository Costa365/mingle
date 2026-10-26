import { AdminPage } from '../features/admin/AdminPage'
import { useCallback, useEffect, useState } from 'react'
import { Link, NavLink, Route, Routes, useParams } from 'react-router-dom'
import { loadGames } from '../api/games'
import type { GameSummary } from '../api/games'
import { RoomEntry, RoomLobby } from '../features/rooms/RoomPages'

type CatalogueState =
  { kind: 'loading' } | { kind: 'error' } | { kind: 'ready'; games: GameSummary[] }

function PeopleIcon({ className }: { className: string }) {
  return <img className={className} src="/people.svg" alt="" aria-hidden="true" />
}

function CatalogueMessage({ state, retry }: { state: CatalogueState; retry: () => void }) {
  if (state.kind === 'loading') {
    return (
      <div className="notice" role="status">
        <span className="loading-dot" /> Gathering the games…
      </div>
    )
  }
  if (state.kind === 'error') {
    return (
      <div className="notice error-notice" role="alert">
        <h3>We couldn't load the games</h3>
        <p>Give it another go in a moment.</p>
        <button className="button" onClick={retry}>
          Try again
        </button>
      </div>
    )
  }
  return (
    <div className="notice" role="status">
      The game shelf is empty for now. Check back soon.
    </div>
  )
}

function Tabletop() {
  return (
    <div className="tabletop" aria-hidden="true">
      <span className="tabletop-label">A GOOD TIME, IN GOOD COMPANY</span>
      <div className="sample-card story-card">
        <span>GET TO KNOW YOUR PEOPLE</span>
        <strong>
          There's a story
          <br />
          behind that
        </strong>
        <div className="card-dots">
          <i />
          <i />
          <i />
        </div>
      </div>
      <div className="sample-card choice-card">
        <span>THIS OR THAT</span>
        <strong>
          Coffee<span className="or">or</span>Tea?
        </strong>
        <span className="card-footer">CHOOSE YOUR SIDE ↗</span>
      </div>
      <div className="play-token">
        let's
        <br />
        play
      </div>
      <div className="scribble">a break worth taking ↗</div>
    </div>
  )
}

function Home({ state, retry }: { state: CatalogueState; retry: () => void }) {
  return (
    <>
      <title>Mingle — Team games</title>
      <section className="hero" aria-labelledby="welcome-heading">
        <div className="hero-copy">
          <p className="eyebrow">
            <PeopleIcon className="people-icon" /> GOOD COMPANY, GREAT CONVERSATIONS
          </p>
          <h1 id="welcome-heading">
            Make room
            <br />
            for a little <em>play</em>
          </h1>
          <p className="hero-description">
            Step out of the daily agenda. Discover a new side of your teammates, one good question
            at a time.
          </p>
          <div className="hero-actions">
            <Link className="button" to="/host">
              Host a room <span aria-hidden="true">↗</span>
            </Link>
            <Link to="/join">Join a room</Link>
            <a href="#games">Explore the games</a>
          </div>
          <p className="small-note">Five ways to connect. Plenty to talk about.</p>
        </div>
        <Tabletop />
      </section>
      <section id="games" className="games-section" aria-labelledby="games-heading">
        <div className="section-heading">
          <div>
            <p className="eyebrow">THE GOOD-TIME COLLECTION</p>
            <h2 id="games-heading">Small games, big connections</h2>
          </div>
          <p>Pick a mood. Meet your people.</p>
        </div>
        {state.kind === 'ready' && state.games.length > 0 ? (
          <div className="game-grid">
            {state.games.map((game, index) => (
              <Link className={`game-card theme-${game.id}`} key={game.id} to={`/games/${game.id}`}>
                <div className="game-card-top">
                  <span className="game-category">{game.category}</span>
                  <span className="game-number">0{index + 1}</span>
                </div>
                <h3>{game.name}</h3>
                <p>{game.description}</p>
                <div className="game-card-bottom">
                  <span>About {game.suggestedMinutes} min</span>
                  <span className="card-arrow" aria-hidden="true">
                    ↗
                  </span>
                </div>
              </Link>
            ))}
          </div>
        ) : (
          <CatalogueMessage state={state} retry={retry} />
        )}
      </section>
      <section className="coming-soon">
        <PeopleIcon className="people-icon" />
        <div>
          <h2>Your people, one room</h2>
          <p>Create a room and invite your team. Playable games are coming next.</p>
        </div>
        <Link to="/how-to-play">
          How it will work <span aria-hidden="true">→</span>
        </Link>
      </section>
    </>
  )
}

function GameDetail({ state, retry }: { state: CatalogueState; retry: () => void }) {
  const { id } = useParams()
  if (state.kind !== 'ready')
    return (
      <div className="page-content">
        <CatalogueMessage state={state} retry={retry} />
      </div>
    )
  const game = state.games.find((candidate) => candidate.id === id)
  if (!game) return <NotFound />
  return (
    <section className={`detail-page theme-${game.id}`}>
      <title>{`${game.name} — Mingle`}</title>
      <Link className="back-link" to="/">
        ← All games
      </Link>
      <p className="eyebrow">{game.category}</p>
      <h1>{game.name}</h1>
      <p className="detail-description">{game.description}</p>
      <div className="game-facts">
        <span>About {game.suggestedMinutes} minutes</span>
        <span>
          {game.minimumPlayers === 1 ? 'Solo preview friendly' : `${game.minimumPlayers}+ players`}
        </span>
        <span>Always okay to pass</span>
      </div>
      <div className="instructions">
        <h2>The idea</h2>
        <p>{game.instructions}</p>
        <p>A facilitator keeps things moving. Share as much or as little as you like.</p>
      </div>
      <div className="availability">
        <strong>Ready to play in your room</strong>
        <p>You can gather your team in a room now. No account is needed to join.</p>
        <Link className="button" to="/host">
          Host a room
        </Link>
      </div>
    </section>
  )
}

function HowToPlay() {
  return (
    <section className="how-page page-content">
      <title>How it works — Mingle</title>
      <p className="eyebrow">A LITTLE LESS WORK, A LITTLE MORE PLAY</p>
      <h1>
        Good company
        <br />
        Simple plans
      </h1>
      <p className="page-intro">
        Create a room, invite your people, and play This or That together.
      </p>
      <ol className="steps">
        <li>
          <span aria-hidden="true">01</span>
          <div>
            <h2>Bring your people</h2>
            <p>
              One person hosts and shares an invite. Everyone else joins with a name. No accounts,
              no fuss.
            </p>
          </div>
        </li>
        <li>
          <span aria-hidden="true">02</span>
          <div>
            <h2>Find your game</h2>
            <p>
              Pick a quick icebreaker or a chance to swap stories. Play on your phone or your
              computer.
            </p>
          </div>
        </li>
        <li>
          <span aria-hidden="true">03</span>
          <div>
            <h2>Go at your own pace</h2>
            <p>
              Your facilitator moves each round along. Passing is always welcome. The conversation
              is the point.
            </p>
          </div>
        </li>
      </ol>
      <Link className="button" to="/">
        Explore the games <span aria-hidden="true">↗</span>
      </Link>
    </section>
  )
}

function NotFound() {
  return (
    <section className="page-content missing-page">
      <title>Page not found — Mingle</title>
      <p className="eyebrow">A LITTLE OFF THE BOARD</p>
      <h1>
        This page isn't
        <br />
        in the deck
      </h1>
      <p>Let's get you back to the games.</p>
      <Link className="button" to="/">
        Back to the games
      </Link>
    </section>
  )
}

export function App() {
  const [state, setState] = useState<CatalogueState>({ kind: 'loading' })
  const [attempt, setAttempt] = useState(0)
  useEffect(() => {
    const controller = new AbortController()
    loadGames(controller.signal).then(
      (games) => {
        if (!controller.signal.aborted) setState({ kind: 'ready', games })
      },
      () => {
        if (!controller.signal.aborted) setState({ kind: 'error' })
      },
    )
    return () => controller.abort()
  }, [attempt])
  const retry = useCallback(() => {
    setState({ kind: 'loading' })
    setAttempt((value) => value + 1)
  }, [])

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <header className="site-header">
        <Link className="wordmark" to="/" aria-label="Mingle Team games home">
          <PeopleIcon className="brand-mark" />
          <span className="wordmark-copy">
            <span>Mingle</span> <span className="brand-tagline">Team games</span>
          </span>
        </Link>
        <nav aria-label="Main navigation">
          <NavLink to="/" end>
            The games
          </NavLink>
          <NavLink to="/how-to-play">How it works</NavLink>
        </nav>
      </header>
      <main id="main" tabIndex={-1}>
        <Routes>
          <Route path="/admin" element={<AdminPage />} />
          <Route path="/host" element={<RoomEntry create />} />
          <Route path="/join" element={<RoomEntry create={false} />} />
          <Route path="/rooms/:id" element={<RoomLobby />} />
          <Route path="/" element={<Home state={state} retry={retry} />} />
          <Route path="/games/:id" element={<GameDetail state={state} retry={retry} />} />
          <Route path="/how-to-play" element={<HowToPlay />} />
          <Route path="*" element={<NotFound />} />
        </Routes>
      </main>
      <footer className="site-footer">
        <span>A little play goes a long way.</span>
        <span>
          Made for people, wherever they work. <Link to="/admin">Administration</Link>
        </span>
      </footer>
    </div>
  )
}
