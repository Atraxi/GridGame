import { useState, type FormEvent } from "react"
import { Link, useNavigate, useSearchParams } from "react-router"
import { isGuest, postToUsers, startNewGuestSession } from "./auth"
import "./LoginPage.css"

type Mode = 'create' | 'login'

export default function LoginPage() {
	const [searchParams] = useSearchParams()
	//Set when an expired session bounced the user here (see loaders.ts) - they're returned to where they were after
	const expired = searchParams.get('expired') === '1'
	const [mode, setMode] = useState<Mode>(expired ? 'login' : 'create')
	const [form, setForm] = useState({ userName: '', email: '', password: '' })
	const [error, setError] = useState<string | null>(null)
	const navigate = useNavigate()
	const returnTo = searchParams.get('returnTo')
	//Only ever an in-app path, so this page can't be used to bounce someone off to another site
	const safeReturnTo = returnTo?.startsWith('/') && !returnTo.startsWith('//') ? returnTo : null

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
		postToUsers(mode === 'create' ? 'Promote' : 'Login', form)
			.then(() => navigate(safeReturnTo ?? '/account'))
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
					<p>Your session has expired. Log in again to pick up where you left off.</p>
					<p>
						Or <button type="button" className="auth-page__link-button" onClick={continueAsGuest}>continue as a new guest</button> instead
						- you'll start fresh, without this account's games or stats.
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
		</section>
	)
}
