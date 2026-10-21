import { useState, type FormEvent } from "react"
import { Link, useNavigate, useSearchParams } from "react-router"
import { requestPasswordReset, resetPassword } from "./auth"
import "./LoginPage.css"

const MIN_PASSWORD_LENGTH = 12

/** /forgot-password - asks for a reset link by user name or email address */
export function ForgotPasswordPage() {
	const [userNameOrEmail, setUserNameOrEmail] = useState('')
	const [sending, setSending] = useState(false)
	const [sent, setSent] = useState(false)
	const [error, setError] = useState<string | null>(null)

	const submit = (event: FormEvent) => {
		event.preventDefault()
		setSending(true)
		setError(null)
		requestPasswordReset(userNameOrEmail.trim())
			.then(() => setSent(true))
			.catch(err => setError(err.message))
			.finally(() => setSending(false))
	}

	return (
		<section className="auth-page">
			<h1>Forgot your password?</h1>
			{sent
				? <div className="auth-page__notice">
					<p>
						If an account matches, a link to choose a new password is on its way to the email address it was
						created with. It works for an hour.
					</p>
					<p>Nothing arrived? Check your spam folder, or wait a couple of minutes and ask again.</p>
				</div>
				: <form className="auth-page__form" onSubmit={submit}>
					<p>Enter your user name or email address, and we'll email you a link to choose a new password.</p>
					<label className="auth-page__field auth-page__field--plain">
						User name or email
						<input required value={userNameOrEmail} autoComplete="username" onChange={event => setUserNameOrEmail(event.target.value)} />
					</label>
					<button type="submit" disabled={sending}>{sending ? 'Sending...' : 'Email me a link'}</button>
				</form>}
			{error && <p className="auth-page__error">{error}</p>}
			<p className="auth-page__aside"><Link to="/login">Back to log in</Link></p>
		</section>
	)
}

/** /reset-password?token=... - where the emailed link lands */
export function ResetPasswordPage() {
	const [searchParams] = useSearchParams()
	const token = searchParams.get('token')
	const navigate = useNavigate()
	const [password, setPassword] = useState('')
	const [confirmation, setConfirmation] = useState('')
	const [saving, setSaving] = useState(false)
	const [error, setError] = useState<string | null>(null)

	if (!token) {
		return (
			<section className="auth-page">
				<h1>Reset your password</h1>
				<p>This link is incomplete - try opening it from the email again, or <Link to="/forgot-password">ask for a new one</Link>.</p>
			</section>
		)
	}

	const mismatch = confirmation.length > 0 && password !== confirmation

	const submit = (event: FormEvent) => {
		event.preventDefault()
		if (password !== confirmation) {
			return
		}
		setSaving(true)
		setError(null)
		resetPassword(token, password)
			.then(() => navigate('/account', { replace: true }))
			.catch(err => { setError(err.message); setSaving(false) })
	}

	return (
		<section className="auth-page">
			<h1>Choose a new password</h1>
			<form className="auth-page__form" onSubmit={submit}>
				<label className="auth-page__field auth-page__field--plain">
					New password
					<input
						type="password" required minLength={MIN_PASSWORD_LENGTH} autoComplete="new-password"
						value={password} onChange={event => setPassword(event.target.value)}
					/>
				</label>
				<label className="auth-page__field auth-page__field--plain">
					Type it again
					<input
						type="password" required autoComplete="new-password"
						value={confirmation} onChange={event => setConfirmation(event.target.value)}
					/>
				</label>
				<p className="auth-page__hint">
					At least {MIN_PASSWORD_LENGTH} characters. Saving signs you in here, and signs this account out everywhere else.
				</p>
				{mismatch && <p className="auth-page__error">The two passwords don't match.</p>}
				<button type="submit" disabled={saving || mismatch}>{saving ? 'Saving...' : 'Save new password'}</button>
			</form>
			{error && (
				<p className="auth-page__error">
					{error} {error.includes('expired') && <Link to="/forgot-password">Ask for a new link</Link>}
				</p>
			)}
		</section>
	)
}
