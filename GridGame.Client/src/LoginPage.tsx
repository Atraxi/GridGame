import { useState, type FormEvent } from "react"
import { Link, useNavigate, useSearchParams } from "react-router"
import { claimGuest, getGuestRefreshToken, guestGameCount, isGuest, postToUsers, startNewGuestSession } from "./auth"
import "./LoginPage.css"

type Mode = 'create' | 'login'

export default function LoginPage() {
	const [searchParams] = useSearchParams()
	//Set when an expired session bounced the user here (see loaders.ts) - they're returned to where they were after
	const expired = searchParams.get('expired') === '1'
	const [mode, setMode] = useState<Mode>(expired ? 'login' : 'create')
	const [form, setForm] = useState({ userName: '', email: '', password: '' })
	const [error, setError] = useState<string | null>(null)
	//Set after logging in from a guest session that had played games, which the login would otherwise abandon
	const [guestToClaim, setGuestToClaim] = useState<{ token: string, games: number } | null>(null)
	const navigate = useNavigate()
	const returnTo = searchParams.get('returnTo')
	//Only ever an in-app path, so this page can't be used to bounce someone off to another site
	const safeReturnTo = returnTo?.startsWith('/') && !returnTo.startsWith('//') ? returnTo : null

	const finish = () => navigate(safeReturnTo ?? '/account')

	if (guestToClaim) {
		const { token, games } = guestToClaim
		return (
			<section className="auth-page">
				<h1>Keep your guest games?</h1>
				<p>This guest session has played {games} game{games === 1 ? '' : 's'}. Add {games === 1 ? 'it' : 'them'} to your account, or lose {games === 1 ? 'it' : 'them'}.</p>
				<div className="auth-page__form">
					<button type="button" onClick={() => claimGuest(token).then(finish).catch(err => setError(err.message))}>Add them to my account</button>
					<button type="button" onClick={finish}>No thanks</button>
				</div>
				{error && <p className="auth-page__error">{error}</p>}
			</section>
		)
	}

	if (!isGuest()) {
		return (
			<section className="auth-page">
				<h1>You're already signed in</h1>
				<p><Link to="/account">Go to your account</Link></p>
			</section>
		)
	}

	const field = (key: keyof typeof form, type = 'text') => (
		<label className="auth-page__field">
			{key}
			<input
				type={type}
				required
				value={form[key]}
				onChange={event => setForm({ ...form, [key]: event.target.value })}
			/>
		</label>
	)

	const submit = (event: FormEvent) => {
		event.preventDefault()
		//Creating an account promotes the guest in place, so only logging in to an existing one leaves a guest behind
		const guestToken = mode === 'login' ? getGuestRefreshToken() : null
		postToUsers(mode === 'create' ? 'Promote' : 'Login', form)
			.then(async () => {
				const games = guestToken ? await guestGameCount(guestToken).catch(() => 0) : 0
				if (guestToken && games > 0) {
					setGuestToClaim({ token: guestToken, games })
				} else {
					finish()
				}
			})
			.catch(err => setError(err.message))
	}

	const continueAsGuest = () => {
		startNewGuestSession()
			.then(() => navigate(safeReturnTo ?? '/'))
			.catch(err => setError(err.message))
	}

	return (
		<section className="auth-page">
			{expired && (
				<div className="auth-page__notice">
					<p>Session expired. Log in to continue.</p>
					<p>
						Or <button type="button" className="auth-page__link-button" onClick={continueAsGuest}>continue as a new guest</button> instead
						- you'll start fresh.
					</p>
				</div>
			)}
			<h1>{mode === 'create' ? 'Create account' : 'Log in'}</h1>
			<div className="auth-page__tabs">
				<button type="button" className={mode === 'create' ? 'active' : ''} onClick={() => setMode('create')}>Create account</button>
				<button type="button" className={mode === 'login' ? 'active' : ''} onClick={() => setMode('login')}>Log in</button>
			</div>
			<form className="auth-page__form" onSubmit={submit}>
				{field('userName')}
				{mode === 'create' && field('email', 'email')}
				{field('password', 'password')}
				<button type="submit">{mode === 'create' ? 'Create account' : 'Log in'}</button>
			</form>
			{error && <p className="auth-page__error">{error}</p>}
			{mode === 'login' && <p className="auth-page__aside"><Link to="/forgot-password">Forgot your password?</Link></p>}
		</section>
	)
}
