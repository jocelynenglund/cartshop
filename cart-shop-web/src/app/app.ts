import { Component, signal } from '@angular/core';

import { CreateCart } from './feature/cart/commands/create-cart/create-cart';
import { GetCart } from './feature/cart/queries/get-cart/get-cart';
import { ListSubmittedCarts } from './feature/cart/queries/list-submitted-carts/list-submitted-carts';

import { SetStock } from './feature/inventory/commands/set-stock/set-stock';
import { StockLevel } from './feature/inventory/queries/stock-level/stock-level';

// The root component is now pure composition: it wires the slice components
// together, mirroring how CartShop.ApiService's Program.cs is pure host
// composition over the slice handlers.
@Component({
  selector: 'app-root',
  imports: [CreateCart, GetCart, ListSubmittedCarts, SetStock, StockLevel],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  cartId = signal<string | null>(null);

  // Bump these signals to make dependent components re-fetch.
  cartsTick = signal(0);
  stockTick = signal(0);

  // Stocked SKUs we want to display in the stock summary.
  watchedSkus = signal<string[]>([]);

  onCartCreated(id: string) {
    this.cartId.set(id);
  }

  onCartChanged() {
    // Any cart write may have moved inventory; bump the stock tick.
    this.stockTick.update(v => v + 1);
  }

  onCartSubmitted() {
    this.cartsTick.update(v => v + 1);
  }

  onStockSet(sku: string) {
    if (!this.watchedSkus().includes(sku)) {
      this.watchedSkus.update(list => [...list, sku]);
    }
    this.stockTick.update(v => v + 1);
  }

  discardCart() { this.cartId.set(null); }
}
