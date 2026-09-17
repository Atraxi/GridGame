import { useState, type FormEvent } from "react"
import { Link, useNavigate } from "react-router"
import { isGuest, postToUsers } from "./auth"
import "./LoginPage.css"

type Mode = 'create' | 'login'

export default function LoginPage() {
	const [mode, setMode] = useState<Mode>('create')
	const [form, setForm] = useState({ userName: '', email: '', password: '' })
	const [error, setError] = useState<string | null>(null)
	const navigate = useNavigate()

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
			.then(() => navigate('/account'))
			.catch(err => setError(err.message))
	}

	return (
		<section className="auth-page">
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
