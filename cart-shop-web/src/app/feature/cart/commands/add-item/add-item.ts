import { Component, inject, input, output, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';

// Mirrors CartShop.Core/Feature/Cart/Commands/AddItem/Handler.cs
interface AddItemRequest {
  sku: string;
  displayName: string;
  quantity: number;
  unitPrice: number;
}

@Component({
  selector: 'cs-add-item',
  imports: [FormsModule],
  template: `
    <fieldset>
      <legend>Add item</legend>
      <div class="row item-row">
        <input [ngModel]="sku()" (ngModelChange)="sku.set($event)" placeholder="SKU" />
        <input [ngModel]="displayName()" (ngModelChange)="displayName.set($event)" placeholder="Display name" />
        <input type="number" min="1" [ngModel]="quantity()" (ngModelChange)="quantity.set(+$event)" placeholder="Qty" />
        <input type="number" step="0.01" min="0" [ngModel]="unitPrice()" (ngModelChange)="unitPrice.set(+$event)" placeholder="Unit price" />
        <button (click)="submit()" [disabled]="busy()">Add</button>
      </div>
      @if (error()) { <p class="error-inline">{{ error() }}</p> }
    </fieldset>
  `,
})
export class AddItem {
  private http = inject(HttpClient);

  cartId = input.required<string>();

  sku = signal('');
  displayName = signal('');
  quantity = signal(1);
  unitPrice = signal(0);
  busy = signal(false);
  error = signal<string | null>(null);

  itemAdded = output<void>();

  async submit() {
    const skuVal = this.sku().trim();
    if (!skuVal) { this.error.set('SKU is required'); return; }
    this.error.set(null);
    this.busy.set(true);
    try {
      const body: AddItemRequest = {
        sku: skuVal,
        displayName: this.displayName().trim() || skuVal,
        quantity: Number(this.quantity()) || 1,
        unitPrice: Number(this.unitPrice()) || 0,
      };
      await this.http.post(`/api/carts/${this.cartId()}/items`, body).toPromise();
      this.sku.set('');
      this.displayName.set('');
      this.quantity.set(1);
      this.unitPrice.set(0);
      this.itemAdded.emit();
    } catch (e: any) {
      const errBody = e?.error;
      this.error.set(errBody?.error
        ? (errBody.available != null
            ? `${errBody.error} (asked ${errBody.requested}, available ${errBody.available})`
            : errBody.error)
        : e?.message ?? 'Failed');
    } finally {
      this.busy.set(false);
    }
  }
}
