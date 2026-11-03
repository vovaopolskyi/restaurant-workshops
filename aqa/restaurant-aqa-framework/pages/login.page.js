import { expect } from '@playwright/test';
import { required } from '../support/env.js';

export class LoginPage {
  constructor(page) {
    this.page = page;
    this.form = page.locator('app-login-form');
    this.email = this.form.locator('input[formcontrolname="email"]');
    this.password = this.form.locator('input[formcontrolname="password"]');
    this.signIn = this.form.getByRole('button', { name: 'Sign In', exact: true });
  }

  async login(email, password) {
    await this.page.goto(new URL('/login', required('BASE_URL')).href);
    await expect(this.form).toBeVisible();
    await this.email.fill(email);
    await this.password.fill(password);
    const signedIn = this.page.waitForResponse((response) =>
      response.request().method() === 'POST' && new URL(response.url()).pathname.endsWith('/signin'));
    await this.signIn.click();
    expect((await signedIn).ok(), 'Real UI sign-in should succeed').toBe(true);
    // Observe completion of /me hydration; authentication is never injected by this POM.
    await this.page.waitForFunction(() =>
      Boolean(localStorage.getItem('auth')) && Boolean(localStorage.getItem('user')));
    await expect(this.form).toBeHidden();
  }
}
