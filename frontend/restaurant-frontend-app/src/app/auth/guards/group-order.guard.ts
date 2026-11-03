import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { isOwnerDemoPreview } from '@app/shared/config/owner-preview';
import { AuthGuard } from './auth.guard';

export const groupOrderGuard: CanActivateFn = (route) => {
  if (isOwnerDemoPreview() && route.paramMap.get('reservationId') === 'demo-reservation') {
    return true;
  }
  return inject(AuthGuard).canActivate(route);
};
