import { isDevMode } from '@angular/core';

export function isOwnerDemoPreview(): boolean {
  return isDevMode() && window.location.pathname.startsWith('/preview/workshop6/');
}
