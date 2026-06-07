export interface UserService {
  getUser(id: number): Promise<User>;
  readonly count: number;
}

export class HttpUserService implements UserService {
  getUser(id: number): Promise<User> {
    return Promise.resolve(new User());
  }
}

export function createService(): UserService {
  return new HttpUserService();
}

export const makeHandler = (route: string) => (req: Request): Response => handle(route, req);

export const loadAsync = async (id: number): Promise<User> => fetchUser(id);

export enum Role {
  Admin,
  Editor,
  Viewer,
}

export default function bootstrap(): void {}
