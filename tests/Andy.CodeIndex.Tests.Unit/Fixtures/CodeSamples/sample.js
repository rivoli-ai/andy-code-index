export class Widget extends Base {
  constructor(id) {
    this.id = id;
  }

  async render(target) {
    return target;
  }

  static create(id) {
    return new Widget(id);
  }
}

export function build(a, b) {
  return a + b;
}

export const scale = (value, factor = 2) => value * factor;

export const fetchData = async (url) => {
  return await fetch(url);
};

function internalHelper(x) {
  return x;
}

export default class App {}
