import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ToastService } from '../services/toast/toast.service';
import { AuthenticationService } from './services/authentication.service';
import { SKIP_ERROR_TOAST } from './skip-error-toast.context';
import { tokenInterceptor } from './token.interceptor';

const URL = 'https://test.api/reservations';

describe('tokenInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let toastService: jasmine.SpyObj<ToastService>;

  beforeEach(() => {
    toastService = jasmine.createSpyObj<ToastService>('ToastService', ['error']);
    const authService = jasmine.createSpyObj<AuthenticationService>('AuthenticationService', [
      'getTokens',
    ]);
    authService.getTokens.and.returnValue(null);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([tokenInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toastService },
        { provide: AuthenticationService, useValue: authService },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  for (const path of ['/guest/groups/group-1/sessions', '/guest/groups/group-1/sessions/guest-1']) {
    it(`adds the Owner token to ${path}`, () => {
      const auth = TestBed.inject(AuthenticationService) as jasmine.SpyObj<AuthenticationService>;
      auth.getTokens.and.returnValue({ idToken: 'owner-id-token', accessToken: 'owner-access-token' });
      const url = `https://test.api${path}`;
      http.request(path.endsWith('guest-1') ? 'DELETE' : 'GET', url).subscribe();
      const request = httpMock.expectOne(url);
      expect(request.request.headers.get('Authorization')).toBe('Bearer owner-id-token');
      request.flush({ participants: [], total: 0 });
    });
  }

  it('preserves a guest cart token and body instead of replacing it with Owner credentials', () => {
    const auth = TestBed.inject(AuthenticationService) as jasmine.SpyObj<AuthenticationService>;
    auth.getTokens.and.returnValue({ idToken: 'owner-id-token', accessToken: 'owner-access-token' });
    const url = 'https://test.api/guest/sessions/guest-1/dishes';
    const body = { dishId: 'dish-1', quantity: 2 };
    http.post(url, body, { headers: { Authorization: 'Bearer guest-token' } }).subscribe();
    const request = httpMock.expectOne(url);
    expect(request.request.headers.get('Authorization')).toBe('Bearer guest-token');
    expect(request.request.body).toEqual(body);
    request.flush({});
  });

  it('keeps polling failures silent for Owner session requests', () => {
    const url = 'https://test.api/guest/groups/group-1/sessions';
    http.get(url, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
      .subscribe({ error: () => undefined });
    httpMock.expectOne(url).flush({}, { status: 500, statusText: 'Server Error' });
    expect(toastService.error).not.toHaveBeenCalled();
  });

  it('shows the server message in a toast for a 400 response', () => {
    http.post(URL, {}).subscribe({ error: () => undefined });

    httpMock
      .expectOne(URL)
      .flush({ message: 'Overlaps' }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.error).toHaveBeenCalledWith('Overlaps', 'Error');
  });

  it('does not show a toast but still rethrows when SKIP_ERROR_TOAST is set', () => {
    let receivedStatus: number | undefined;
    http
      .post(URL, {}, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
      .subscribe({ error: (error) => (receivedStatus = error.status) });

    httpMock
      .expectOne(URL)
      .flush({ message: 'Overlaps' }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.error).not.toHaveBeenCalled();
    expect(receivedStatus).toBe(400);
  });
});
