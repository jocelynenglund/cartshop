import { Component, inject, input, output, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';

// Mirrors CartShop.Core/Feature/Cart/Commands/RemoveItem/Handler.cs
@Component({
  selector: 'cs-remove-item',
  template: `
    <button class="link" (click)="submit()" [disabled]="busy()">remove</button>
  `,
})
export class RemoveItem {
  private http = inject(HttpClient);

  cartId = input.required<string>();
  itemId = input.required<string>();

  busy = signal(false);
  itemRemoved = output<void>();

  async submit() {
    this.busy.set(true);
    try {
      await this.http.delete(`/api/carts/${this.cartId()}/items/${this.itemId()}`).toPromise();
      this.itemRemoved.emit();
    } finally {
      this.busy.set(false);
    }
  }
}
